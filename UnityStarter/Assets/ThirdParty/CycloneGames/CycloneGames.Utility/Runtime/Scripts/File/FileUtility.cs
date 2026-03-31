using System;
using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using CycloneGames.Logger;
using UnityEngine; // For Application.platform and platform-specific defines

namespace CycloneGames.Utility.Runtime
{
    public enum HashAlgorithmType
    {
        MD5,
        SHA256,
        XxHash64
    }

    /// <summary>
    /// Thread-safe, allocation-optimized file utility for hashing, comparing, and copying files.
    ///
    /// Thread Safety:
    ///   All public methods are safe to call concurrently from multiple threads.
    ///   IncrementalHash is created per-call (not ThreadLocal) — see GetHashAlgorithmName doc for rationale.
    ///
    /// Memory Strategy:
    ///   - Read buffers: ArrayPool rent/return (zero GC pressure on hot paths).
    ///   - Hash buffers: ArrayPool for async methods, stackalloc for synchronous methods.
    ///   - Hex conversion: stackalloc char[] + lookup table (single string allocation).
    ///   - FileStream: AsyncFileStreamBufferSize (4096) avoids double-buffering with external buffers.
    ///
    /// Platform Notes:
    ///   - WebGL: async operations run on main thread; only persistentDataPath is reliable for file I/O.
    ///   - Android: StreamingAssets are inside APK, use UnityWebRequest via FilePathUtility instead.
    ///   - Mobile: larger read buffers (81920) reduce syscall overhead on slower flash storage.
    /// </summary>
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

        // Hex lookup table: avoids per-byte ToString("x2") allocation
        private static readonly char[] HexChars = "0123456789abcdef".ToCharArray();

        // When using FileOptions.Asynchronous with external ArrayPool buffers,
        // the FileStream's internal buffer is redundant for large sequential reads.
        // 4096 (one memory page) avoids double-buffering waste (~60KB saved per FileStream).
        private const int AsyncFileStreamBufferSize = 4096;

        // On WebGL and single-threaded runtimes, async I/O runs synchronously on the main thread.
        // Without periodic yields, large-file operations (e.g., 1 GB) freeze the browser/UI for
        // the entire duration. We yield back to the caller every N loop iterations to allow frame
        // rendering and event processing. This has negligible cost on threaded platforms because
        // Task.Yield() simply re-queues the continuation on the thread pool.
#if UNITY_WEBGL
        private const int YieldIntervalChunks = 4;   // ~512 KB between yields (128 KB × 4)
#else
        private const int YieldIntervalChunks = 32;  // ~2 MB between yields (64 KB × 32)
#endif

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static byte[] GetReadBuffer() => ArrayPool<byte>.Shared.Rent(ReadBufferSize);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ReturnReadBuffer(byte[] buffer) => ArrayPool<byte>.Shared.Return(buffer);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetHashSizeInBytes(HashAlgorithmType algorithmType)
        {
            switch (algorithmType)
            {
                case HashAlgorithmType.MD5: return 16;
                case HashAlgorithmType.SHA256: return 32;
                case HashAlgorithmType.XxHash64: return 8;
                default: throw new ArgumentOutOfRangeException(nameof(algorithmType));
            }
        }

        /// <summary>
        /// Returns the .NET HashAlgorithmName for a given HashAlgorithmType.
        /// IncrementalHash is created per-call (not cached in ThreadLocal) because:
        /// 1. Async methods with ConfigureAwait(false) can resume on different threads,
        ///    making ThreadLocal unsafe for stateful objects across await boundaries.
        ///    Thread A's hasher could be used concurrently by Thread A (new task) and
        ///    Thread B (continuation of a prior task that captured the local variable).
        /// 2. If an exception occurs between AppendData and TryGetHashAndReset,
        ///    a cached hasher retains corrupt partial state for subsequent calls.
        /// 3. IncrementalHash.CreateHash allocation is negligible vs file I/O latency.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static HashAlgorithmName GetHashAlgorithmName(HashAlgorithmType type)
        {
            switch (type)
            {
                case HashAlgorithmType.MD5: return HashAlgorithmName.MD5;
                case HashAlgorithmType.SHA256: return HashAlgorithmName.SHA256;
                case HashAlgorithmType.XxHash64: throw new InvalidOperationException($"{nameof(HashAlgorithmType.XxHash64)} does not use IncrementalHash. Use XxHash64 struct directly.");
                default: throw new ArgumentOutOfRangeException(nameof(type));
            }
        }

        /// <summary>
        /// Converts a byte span to a lowercase hex string using a lookup table.
        /// Allocates exactly one string. No StringBuilder, no per-byte ToString("x2").
        /// </summary>
        public static string ToHexString(ReadOnlySpan<byte> hashBytes)
        {
            if (hashBytes.IsEmpty) return string.Empty;

            int len = hashBytes.Length;
            Span<char> chars = len <= 32
                ? stackalloc char[len * 2]  // MD5=32 chars, SHA256=64 chars — both fit on stack
                : new char[len * 2];

            for (int i = 0; i < len; i++)
            {
                byte b = hashBytes[i];
                chars[i * 2]     = HexChars[b >> 4];
                chars[i * 2 + 1] = HexChars[b & 0xF];
            }

            return new string(chars);
        }

        public static async Task<bool> AreFilesEqualAsync(string filePath1, string filePath2,
            HashAlgorithmType algorithm = HashAlgorithmType.SHA256, CancellationToken cancellationToken = default)
        {
            return await AreFilesEqualAsync(filePath1, filePath2, algorithm, null, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Compares two files for equality. Reports progress (0.0 to 1.0) via the optional IProgress parameter.
        /// Files larger than LargeFileThreshold use chunk-by-chunk comparison; smaller files use hash comparison.
        /// </summary>
        public static async Task<bool> AreFilesEqualAsync(string filePath1, string filePath2,
            HashAlgorithmType algorithm, IProgress<float> progress, CancellationToken cancellationToken = default)
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
                if (string.Equals(filePath1, filePath2, StringComparison.Ordinal)) { progress?.Report(1f); return true; }

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
                if (fileInfo1.Length == 0) { progress?.Report(1f); return true; }

                cancellationToken.ThrowIfCancellationRequested();

                bool areEqual = fileInfo1.Length > LargeFileThreshold
                    ? await AreFilesEqualByChunksAsync(filePath1, filePath2, progress, cancellationToken).ConfigureAwait(false)
                    : await AreFilesEqualByHashAsync(filePath1, filePath2, algorithm, progress, cancellationToken).ConfigureAwait(false);

                return areEqual;
            }
            catch (OperationCanceledException) { CLogger.LogDebug($"{DEBUG_FLAG} File comparison cancelled."); throw; }
            catch (Exception ex) { CLogger.LogWarning($"{DEBUG_FLAG} Exception during file comparison: {ex.Message}"); return false; }
            finally { sw.Stop(); CLogger.LogDebug($"{DEBUG_FLAG} File comparison '{Path.GetFileName(filePath1)}' vs '{Path.GetFileName(filePath2)}' took {sw.ElapsedMilliseconds}ms."); }
        }

        private static async Task<bool> AreFilesEqualByHashAsync(string filePath1, string filePath2,
            HashAlgorithmType algorithm, IProgress<float> progress, CancellationToken cancellationToken)
        {
            int hashSize = GetHashSizeInBytes(algorithm);
            byte[] hash1Buffer = ArrayPool<byte>.Shared.Rent(hashSize);
            byte[] hash2Buffer = ArrayPool<byte>.Shared.Rent(hashSize);

            try
            {
                progress?.Report(0f);
                bool success1 = await ComputeFileHashAsync(filePath1, algorithm, hash1Buffer.AsMemory(0, hashSize), cancellationToken).ConfigureAwait(false);
                progress?.Report(0.5f);
                cancellationToken.ThrowIfCancellationRequested();
                bool success2 = await ComputeFileHashAsync(filePath2, algorithm, hash2Buffer.AsMemory(0, hashSize), cancellationToken).ConfigureAwait(false);
                progress?.Report(1f);

                if (!success1 || !success2)
                {
                    CLogger.LogWarning($"{DEBUG_FLAG} Hash computation failed for one or both files.");
                    return false;
                }
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

        private static async Task<bool> AreFilesEqualByChunksAsync(string filePath1, string filePath2, IProgress<float> progress, CancellationToken cancellationToken)
        {
            byte[] buffer1 = GetReadBuffer();
            byte[] buffer2 = GetReadBuffer();
            try
            {
                using (var fs1 = new FileStream(filePath1, FileMode.Open, FileAccess.Read, FileShare.Read, AsyncFileStreamBufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan))
                using (var fs2 = new FileStream(filePath2, FileMode.Open, FileAccess.Read, FileShare.Read, AsyncFileStreamBufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    long totalBytes = fs1.Length;
                    int bytesRead1;
                    int chunkCount = 0;
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
                        if (!CompareByteArrays(buffer1, buffer2, bytesRead1)) return false;
                        progress?.Report(totalBytes > 0 ? (float)fs1.Position / totalBytes : 1f);
                        if (++chunkCount % YieldIntervalChunks == 0)
                            await Task.Yield();
                    }
                    return true;
                }
            }
            catch (OperationCanceledException) { CLogger.LogDebug($"{DEBUG_FLAG} Chunk comparison cancelled."); throw; }
            catch (Exception ex) { CLogger.LogWarning($"{DEBUG_FLAG} Error comparing files by chunks: {ex.Message}"); return false; }
            finally { ReturnReadBuffer(buffer1); ReturnReadBuffer(buffer2); }
        }

        public static async Task<bool> ComputeFileHashAsync(string filePath, HashAlgorithmType algorithmType,
            Memory<byte> hashBuffer, CancellationToken cancellationToken)
        {
            return await ComputeFileHashAsync(filePath, algorithmType, hashBuffer, null, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Computes the hash of a file asynchronously. Reports progress (0.0 to 1.0) via the optional IProgress parameter.
        /// </summary>
        public static async Task<bool> ComputeFileHashAsync(string filePath, HashAlgorithmType algorithmType,
            Memory<byte> hashBuffer, IProgress<float> progress, CancellationToken cancellationToken)
        {
#if UNITY_WEBGL
            if (!filePath.Contains(Application.persistentDataPath)) { CLogger.LogWarning($"{DEBUG_FLAG} ComputeFileHashAsync on WebGL for non-persistentDataPath is unreliable."); }
#endif
            if (hashBuffer.Length < GetHashSizeInBytes(algorithmType)) { CLogger.LogError($"{DEBUG_FLAG} Hash buffer too small."); return false; }

            var fileReadBuffer = GetReadBuffer();
            try
            {
                using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, AsyncFileStreamBufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    long totalBytes = stream.Length;
                    if (algorithmType == HashAlgorithmType.XxHash64)
                    {
                        var xxHasher = XxHash64.Create();
                        int bytesRead;
                        int chunkCount = 0;
                        while ((bytesRead = await stream.ReadAsync(fileReadBuffer, 0, fileReadBuffer.Length, cancellationToken).ConfigureAwait(false)) > 0)
                        {
                            xxHasher.Append(fileReadBuffer, 0, bytesRead);
                            progress?.Report(totalBytes > 0 ? (float)stream.Position / totalBytes : 1f);
                            cancellationToken.ThrowIfCancellationRequested();
                            if (++chunkCount % YieldIntervalChunks == 0)
                                await Task.Yield();
                        }
                        return xxHasher.TryWriteHash(hashBuffer.Span);
                    }

                    using (var incrementalHasher = IncrementalHash.CreateHash(GetHashAlgorithmName(algorithmType)))
                    {
                        int bytesRead;
                        int chunkCount = 0;
                        while ((bytesRead = await stream.ReadAsync(fileReadBuffer, 0, fileReadBuffer.Length, cancellationToken).ConfigureAwait(false)) > 0)
                        {
                            incrementalHasher.AppendData(fileReadBuffer, 0, bytesRead);
                            progress?.Report(totalBytes > 0 ? (float)stream.Position / totalBytes : 1f);
                            cancellationToken.ThrowIfCancellationRequested();
                            if (++chunkCount % YieldIntervalChunks == 0)
                                await Task.Yield();
                        }
                        return incrementalHasher.TryGetHashAndReset(hashBuffer.Span, out _);
                    }
                }
            }
            catch (OperationCanceledException) { CLogger.LogDebug($"{DEBUG_FLAG} Hash computation cancelled for '{filePath}'."); throw; }
            catch (Exception ex) { CLogger.LogError($"{DEBUG_FLAG} Error computing hash for {filePath}: {ex.Message}"); return false; }
            finally { ReturnReadBuffer(fileReadBuffer); }
        }

        /// <summary>
        /// Convenience method: computes a file hash and returns it as a lowercase hex string.
        /// Returns null if hash computation fails.
        /// </summary>
        public static async Task<string> ComputeFileHashToHexStringAsync(string filePath,
            HashAlgorithmType algorithmType = HashAlgorithmType.SHA256, CancellationToken cancellationToken = default)
        {
            return await ComputeFileHashToHexStringAsync(filePath, algorithmType, null, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Convenience method: computes a file hash and returns it as a lowercase hex string.
        /// Reports progress (0.0 to 1.0) via the optional IProgress parameter.
        /// Returns null if hash computation fails.
        /// </summary>
        public static async Task<string> ComputeFileHashToHexStringAsync(string filePath,
            HashAlgorithmType algorithmType, IProgress<float> progress, CancellationToken cancellationToken = default)
        {
            int hashSize = GetHashSizeInBytes(algorithmType);
            byte[] hashBuffer = ArrayPool<byte>.Shared.Rent(hashSize);
            try
            {
                bool success = await ComputeFileHashAsync(filePath, algorithmType, hashBuffer.AsMemory(0, hashSize), progress, cancellationToken).ConfigureAwait(false);
                return success ? ToHexString(hashBuffer.AsSpan(0, hashSize)) : null;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(hashBuffer);
            }
        }

        public static async Task<bool> AreStreamsEqualAsync(Stream stream1, Stream stream2,
            long length1, long length2, HashAlgorithmType algorithm = HashAlgorithmType.SHA256, CancellationToken cancellationToken = default)
        {
            return await AreStreamsEqualAsync(stream1, stream2, length1, length2, algorithm, null, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Compares two streams for equality. Reports progress (0.0 to 1.0) via the optional IProgress parameter.
        /// </summary>
        public static async Task<bool> AreStreamsEqualAsync(Stream stream1, Stream stream2,
            long length1, long length2, HashAlgorithmType algorithm, IProgress<float> progress, CancellationToken cancellationToken = default)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                if (stream1 == null || stream2 == null) return stream1 == stream2;
                if (!stream1.CanRead || !stream2.CanRead) { CLogger.LogWarning($"{DEBUG_FLAG} Stream not readable."); return false; }
                if (length1 != length2) return false;
                if (length1 == 0) { progress?.Report(1f); return true; }

                cancellationToken.ThrowIfCancellationRequested();

                bool areEqual = length1 > LargeFileThreshold
                    ? await AreStreamsEqualByChunksAsync(stream1, stream2, length1, progress, cancellationToken).ConfigureAwait(false)
                    : await AreStreamsEqualByHashAsync(stream1, stream2, algorithm, progress, cancellationToken).ConfigureAwait(false);

                return areEqual;
            }
            catch (OperationCanceledException) { CLogger.LogDebug($"{DEBUG_FLAG} Stream comparison cancelled."); throw; }
            catch (Exception ex) { CLogger.LogWarning($"{DEBUG_FLAG} Exception during stream comparison: {ex.Message}"); return false; }
            finally { sw.Stop(); CLogger.LogDebug($"{DEBUG_FLAG} Stream comparison took {sw.ElapsedMilliseconds}ms."); }
        }

        private static async Task<bool> AreStreamsEqualByHashAsync(Stream stream1, Stream stream2,
            HashAlgorithmType algorithm, IProgress<float> progress, CancellationToken cancellationToken)
        {
            int hashSize = GetHashSizeInBytes(algorithm);
            byte[] hash1Buffer = ArrayPool<byte>.Shared.Rent(hashSize);
            byte[] hash2Buffer = ArrayPool<byte>.Shared.Rent(hashSize);

            try
            {
                progress?.Report(0f);
                bool success1 = await ComputeStreamHashAsync(stream1, algorithm, hash1Buffer.AsMemory(0, hashSize), cancellationToken).ConfigureAwait(false);
                progress?.Report(0.5f);
                cancellationToken.ThrowIfCancellationRequested();
                bool success2 = await ComputeStreamHashAsync(stream2, algorithm, hash2Buffer.AsMemory(0, hashSize), cancellationToken).ConfigureAwait(false);
                progress?.Report(1f);

                if (!success1 || !success2) { CLogger.LogWarning($"{DEBUG_FLAG} Stream hash computation failed."); return false; }
                return CompareHashBuffers(hash1Buffer, hash2Buffer, hashSize);
            }
            catch (Exception ex) { CLogger.LogWarning($"{DEBUG_FLAG} Error comparing streams by hash: {ex.Message}"); return false; }
            finally
            {
                ArrayPool<byte>.Shared.Return(hash1Buffer);
                ArrayPool<byte>.Shared.Return(hash2Buffer);
            }
        }

        private static async Task<bool> AreStreamsEqualByChunksAsync(Stream stream1, Stream stream2, long streamLength, IProgress<float> progress, CancellationToken cancellationToken)
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
                    progress?.Report(streamLength > 0 ? (float)totalBytesCompared / streamLength : 1f);
                    if ((totalBytesCompared / ReadBufferSize) % YieldIntervalChunks == 0)
                        await Task.Yield();
                }
                return totalBytesCompared == streamLength;
            }
            catch (Exception ex) { CLogger.LogWarning($"{DEBUG_FLAG} Error comparing streams by chunks: {ex.Message}"); return false; }
            finally { ReturnReadBuffer(buffer1); ReturnReadBuffer(buffer2); }
        }

        public static async Task<bool> ComputeStreamHashAsync(Stream stream, HashAlgorithmType algorithmType,
            Memory<byte> hashBuffer, CancellationToken cancellationToken)
        {
            return await ComputeStreamHashAsync(stream, algorithmType, hashBuffer, null, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Computes the hash of a stream asynchronously. Reports progress (0.0 to 1.0) via the optional IProgress parameter.
        /// Progress is only reported if the stream supports seeking (CanSeek == true).
        /// </summary>
        public static async Task<bool> ComputeStreamHashAsync(Stream stream, HashAlgorithmType algorithmType,
            Memory<byte> hashBuffer, IProgress<float> progress, CancellationToken cancellationToken)
        {
            if (stream == null || !stream.CanRead) { CLogger.LogError($"{DEBUG_FLAG} Stream null or not readable."); return false; }
            if (hashBuffer.Length < GetHashSizeInBytes(algorithmType)) { CLogger.LogError($"{DEBUG_FLAG} Hash buffer too small."); return false; }

            bool canReportProgress = progress != null && stream.CanSeek;
            long totalBytes = canReportProgress ? stream.Length : 0;

            var readBuffer = GetReadBuffer();
            try
            {
                if (algorithmType == HashAlgorithmType.XxHash64)
                {
                    var xxHasher = XxHash64.Create();
                    int bytesRead;
                    int chunkCount = 0;
                    while ((bytesRead = await stream.ReadAsync(readBuffer, 0, readBuffer.Length, cancellationToken).ConfigureAwait(false)) > 0)
                    {
                        xxHasher.Append(readBuffer, 0, bytesRead);
                        if (canReportProgress) progress.Report(totalBytes > 0 ? (float)stream.Position / totalBytes : 1f);
                        cancellationToken.ThrowIfCancellationRequested();
                        if (++chunkCount % YieldIntervalChunks == 0)
                            await Task.Yield();
                    }
                    return xxHasher.TryWriteHash(hashBuffer.Span);
                }

                using (var incrementalHasher = IncrementalHash.CreateHash(GetHashAlgorithmName(algorithmType)))
                {
                    int bytesRead;
                    int chunkCount = 0;
                    while ((bytesRead = await stream.ReadAsync(readBuffer, 0, readBuffer.Length, cancellationToken).ConfigureAwait(false)) > 0)
                    {
                        incrementalHasher.AppendData(readBuffer, 0, bytesRead);
                        if (canReportProgress) progress.Report(totalBytes > 0 ? (float)stream.Position / totalBytes : 1f);
                        cancellationToken.ThrowIfCancellationRequested();
                        if (++chunkCount % YieldIntervalChunks == 0)
                            await Task.Yield();
                    }
                    return incrementalHasher.TryGetHashAndReset(hashBuffer.Span, out _);
                }
            }
            catch (OperationCanceledException) { CLogger.LogDebug($"{DEBUG_FLAG} Stream hash computation cancelled."); throw; }
            catch (Exception ex) { CLogger.LogError($"{DEBUG_FLAG} Error computing stream hash: {ex.Message}"); return false; }
            finally { ReturnReadBuffer(readBuffer); }
        }

        /// <summary>
        /// Convenience method: computes a stream hash and returns it as a lowercase hex string.
        /// Returns null if hash computation fails.
        /// </summary>
        public static async Task<string> ComputeStreamHashToHexStringAsync(Stream stream,
            HashAlgorithmType algorithmType = HashAlgorithmType.SHA256, CancellationToken cancellationToken = default)
        {
            return await ComputeStreamHashToHexStringAsync(stream, algorithmType, null, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Convenience method: computes a stream hash and returns it as a lowercase hex string.
        /// Reports progress (0.0 to 1.0) via the optional IProgress parameter.
        /// Returns null if hash computation fails.
        /// </summary>
        public static async Task<string> ComputeStreamHashToHexStringAsync(Stream stream,
            HashAlgorithmType algorithmType, IProgress<float> progress, CancellationToken cancellationToken = default)
        {
            int hashSize = GetHashSizeInBytes(algorithmType);
            byte[] hashBuffer = ArrayPool<byte>.Shared.Rent(hashSize);
            try
            {
                bool success = await ComputeStreamHashAsync(stream, algorithmType, hashBuffer.AsMemory(0, hashSize), progress, cancellationToken).ConfigureAwait(false);
                return success ? ToHexString(hashBuffer.AsSpan(0, hashSize)) : null;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(hashBuffer);
            }
        }

        /// <summary>
        /// Synchronous file hash computation. Use when async is not available (e.g., Editor scripts, OnValidate).
        /// Blocks the calling thread until complete. Avoid on the main thread for large files.
        /// </summary>
        public static bool ComputeFileHash(string filePath, HashAlgorithmType algorithmType, Span<byte> hashBuffer)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return false;
            if (hashBuffer.Length < GetHashSizeInBytes(algorithmType)) return false;

            var readBuffer = GetReadBuffer();
            try
            {
                // Sync reads with our own ArrayPool buffer bypass the FileStream internal buffer when
                // readBuffer.Length > bufferSize, so a large internal buffer is wasted heap allocation.
                // Use AsyncFileStreamBufferSize (4096 = one page) to minimize GC, same rationale as async path.
                using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, AsyncFileStreamBufferSize, FileOptions.SequentialScan))
                {
                    if (algorithmType == HashAlgorithmType.XxHash64)
                    {
                        var xxHasher = XxHash64.Create();
                        int bytesRead;
                        while ((bytesRead = stream.Read(readBuffer, 0, readBuffer.Length)) > 0)
                        {
                            xxHasher.Append(readBuffer, 0, bytesRead);
                        }
                        return xxHasher.TryWriteHash(hashBuffer);
                    }

                    using (var incrementalHasher = IncrementalHash.CreateHash(GetHashAlgorithmName(algorithmType)))
                    {
                        int bytesRead;
                        while ((bytesRead = stream.Read(readBuffer, 0, readBuffer.Length)) > 0)
                        {
                            incrementalHasher.AppendData(readBuffer, 0, bytesRead);
                        }
                        return incrementalHasher.TryGetHashAndReset(hashBuffer, out _);
                    }
                }
            }
            catch (Exception ex) { CLogger.LogError($"{DEBUG_FLAG} Error computing hash for {filePath}: {ex.Message}"); return false; }
            finally { ReturnReadBuffer(readBuffer); }
        }

        /// <summary>
        /// Synchronous convenience method: computes a file hash and returns it as a lowercase hex string.
        /// Returns null if hash computation fails. Avoid on the main thread for large files.
        /// </summary>
        public static string ComputeFileHashToHexString(string filePath, HashAlgorithmType algorithmType = HashAlgorithmType.SHA256)
        {
            int hashSize = GetHashSizeInBytes(algorithmType);
            Span<byte> hashBuffer = stackalloc byte[hashSize];
            return ComputeFileHash(filePath, algorithmType, hashBuffer)
                ? ToHexString(hashBuffer)
                : null;
        }

        public static bool AreByteArraysEqualByHash(ReadOnlySpan<byte> content1, ReadOnlySpan<byte> content2, HashAlgorithmType algorithmType)
        {
            if (content1.Length != content2.Length) return false;
            if (content1.IsEmpty) return true;

            if (algorithmType == HashAlgorithmType.XxHash64)
            {
                return XxHash64.HashToUInt64(content1) == XxHash64.HashToUInt64(content2);
            }

            int hashSize = GetHashSizeInBytes(algorithmType);
            Span<byte> hash1Bytes = stackalloc byte[hashSize];
            Span<byte> hash2Bytes = stackalloc byte[hashSize];

            using (var hasher = IncrementalHash.CreateHash(GetHashAlgorithmName(algorithmType)))
            {
                hasher.AppendData(content1);
                hasher.TryGetHashAndReset(hash1Bytes, out _);

                hasher.AppendData(content2);
                hasher.TryGetHashAndReset(hash2Bytes, out _);
            }

            return ((ReadOnlySpan<byte>)hash1Bytes).SequenceEqual(hash2Bytes);
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
            await CopyFileWithComparisonAsync(sourceFilePath, destinationFilePath, comparisonAlgorithm, null, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Copies a file with hash comparison. If the destination already exists and is identical, the copy is skipped.
        /// Reports progress (0.0 to 1.0) via the optional IProgress parameter.
        /// On cancellation, the partial destination file is deleted to avoid corrupt data.
        /// </summary>
        public static async Task CopyFileWithComparisonAsync(string sourceFilePath, string destinationFilePath,
            HashAlgorithmType comparisonAlgorithm, IProgress<float> progress, CancellationToken cancellationToken = default)
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
                    // FileMode.Create truncates existing files, so explicit Delete is unnecessary.
                    if (await AreFilesEqualAsync(sourceFilePath, destinationFilePath, comparisonAlgorithm, cancellationToken).ConfigureAwait(false))
                    {
                        shouldCopy = false;
                    }
                }

                if (!shouldCopy) { CLogger.LogInfo($"{DEBUG_FLAG} Files identical, copy skipped."); return; }
                cancellationToken.ThrowIfCancellationRequested();

                var buffer = GetReadBuffer();
                try
                {
                    using (var sourceStream = new FileStream(sourceFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, AsyncFileStreamBufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan))
                    using (var destinationStream = new FileStream(destinationFilePath, FileMode.Create, FileAccess.Write, FileShare.None, AsyncFileStreamBufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan))
                    {
                        long totalBytes = sourceStream.Length;
                        long bytesCopied = 0;
                        int bytesRead;
                        int chunkCount = 0;
                        while ((bytesRead = await sourceStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false)) > 0)
                        {
                            await destinationStream.WriteAsync(buffer, 0, bytesRead, cancellationToken).ConfigureAwait(false);
                            bytesCopied += bytesRead;
                            progress?.Report(totalBytes > 0 ? (float)bytesCopied / totalBytes : 1f);
                            cancellationToken.ThrowIfCancellationRequested();
                            if (++chunkCount % YieldIntervalChunks == 0)
                                await Task.Yield();
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

        /// <summary>
        /// Compares two byte arrays for equality up to a specified length.
        /// </summary>
        private static bool CompareByteArrays(byte[] array1, byte[] array2, int lengthToCompare = -1)
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

            return new ReadOnlySpan<byte>(array1, 0, lengthToCompare)
                .SequenceEqual(new ReadOnlySpan<byte>(array2, 0, lengthToCompare));
        }
    }
}